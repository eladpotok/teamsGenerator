import { DeleteOutlined, EditOutlined, QuestionCircleOutlined, UserOutlined } from "@ant-design/icons";
import { Avatar, Button, Card, Drawer, List, Modal, Popconfirm } from "antd";
import { useContext, useState } from "react";
import AddPlayerForm from "../../Common/AddPlayerForm";
import AppCheckBox from "../../Common/AppCheckBox";
import { AnalyticsContext } from "../../../Store/AnalyticsContext";
import { UserContext } from "../../../Store/UserContext";
import { PlayersContext } from "../../../Store/PlayersContext";
import { GiSoccerKick, GiSoccerField } from 'react-icons/gi';
import { MdOutlineCancelPresentation } from 'react-icons/md';
import { IoPersonCircle } from 'react-icons/io';
import MyIconButton from "../../Common/MyIconButton";

function MobilePlayersList(props) {
    const analyticsContext = useContext(AnalyticsContext)
    const playersContext = useContext(PlayersContext)
    const userContext = useContext(UserContext)

    const [editPlayerDrawerOpen, setEditPlayerDrawerOpen] = useState(false)
    const [editPlayer, setEditPlayer] = useState(null)

    function playerListItemClicked(clickedPlayer) {
        props.onPlayerRemoved(clickedPlayer)
        analyticsContext.sendAnalyticsEngagement(userContext.user.uid, 'playerRemovedFromList', clickedPlayer)
    }

    function setPlayerArrivedHandler(player, isArrived) {
        props.onPlayerArrived(player, isArrived)
    }

    function openEditPlayerViewHandler(player) {
        const edittedPlayer = { ...player, key: player.key }
        setEditPlayer({...edittedPlayer}); 
        console.log('open edit screen with player', edittedPlayer)
        setEditPlayerDrawerOpen(true)
    }

    function playerSubmittedHandler() {
        const edittedPlayer = { ...editPlayer, key: editPlayer.key }
        console.log('edit player', edittedPlayer)
        playersContext.editPlayer(edittedPlayer)
        analyticsContext.sendAnalyticsEngagement(userContext.user.uid, 'editPlayer', edittedPlayer)
        setEditPlayerDrawerOpen(false)
    }


    return (
        <Card style={{
            height: '100%',  background: 'rgba(255, 247, 251, 0.12)',
            overflow: 'auto',
            margin: '4px',
            // border: '1px solid rgba(140, 140, 140, 0.35)',
          }}>
            
            <List itemLayout="horizontal"   
                  size="small" style={{ marginLeft: '-18px', marginRight: '-18px', marginTop: '-18px', height: '1px' /* Now sure why but this 1px causes the list height not be extended  */}}
                  dataSource={props.players} locale={{emptyText: 
                    <div style={{marginTop: '140px'}}>
                        <GiSoccerField style={{fontSize: '78px', color: 'white'}}/> <br></br>
                        <label style={{color: 'white'}}> No Players Added</label>
                    </div>
                }}
                  renderItem={(player, index) => (
                    <List.Item style={{ backgroundColor: 'rgba(255, 247, 251, 0.12)'}} 
                    actions={[ 
                    <div style={{marginRight: '-10px'}}><MyIconButton onClick={()=>{openEditPlayerViewHandler(player)}} icon={<EditOutlined /> }/></div> , 
                    <div style={{marginRight: '-10px'}}><AppCheckBox  value={player.isArrived} onChanged={(e)=>{setPlayerArrivedHandler(player, e)}} /></div>, 
                    <Popconfirm  icon={<QuestionCircleOutlined style={{ color: 'red' }} />} title='Remove Player' description='Are you sure you want to remove this player?' onConfirm={()=>{playerListItemClicked(player) }}>
                        <div style={{marginRight: '-10px'}}><MyIconButton buttonType='deleteButton'  icon={<DeleteOutlined />}/></div>
                    </Popconfirm>]}>
                            
                        <List.Item.Meta  avatar={ <Avatar style={{ marginTop: '20px', background: 'rgba(255, 247, 251, 0.12)' }}><GiSoccerKick  style={{ fontSize: '30px'}} /></Avatar>}
                                        title={<div style={{ fontSize: '12px', width: '100%', 'textOverflow': 'ellipsis', 'whiteSpace': 'nowrap', 'overflow': 'hidden', marginBottom: '-8px' }}>{player.Name.toUpperCase()}</div>}
                                        description={
                                            <label style={{fontSize: '12px'}}>{player.isArrived ? 'Joined' : 'Absent'}</label>
                                        }/>

                    </List.Item>
                )}>

            </List>


            {props.currentAlgo && <Modal  title={<><GiSoccerKick  style={{  fontSize:'18px', marginBottom: '-4px', marginRight: '4px',  color: '#095c1f', marginLeft: '4px'}} /><label style={{marginLeft: '4px', color: '#095c1f'}}>EDIT PLAYER</label></>} 
                        width='75%' footer={[]} style={{ top: 20 }} closable={false}
                        open={editPlayerDrawerOpen}>
                    <AddPlayerForm player={editPlayer} playersProperties={props.currentAlgo.playerProperties}  onPlayerSubmitted={playerSubmittedHandler} onCancelClicked={() => { setEditPlayerDrawerOpen(false) }}/>
                </Modal>}

        </Card>
    )
}

export default MobilePlayersList;