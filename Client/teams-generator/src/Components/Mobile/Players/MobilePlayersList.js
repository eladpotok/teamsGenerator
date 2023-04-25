import { DeleteOutlined, EditOutlined, QuestionCircleOutlined, UserOutlined } from "@ant-design/icons";
import { Avatar, Button, Card, Drawer, List, Popconfirm } from "antd";
import { useContext, useState } from "react";
import AddPlayerForm from "../../Common/AddPlayerForm";
import AppCheckBox from "../../Common/AppCheckBox";
import { AnalyticsContext } from "../../../Store/AnalyticsContext";
import { UserContext } from "../../../Store/UserContext";

function MobilePlayersList(props) {
    const analyticsContext = useContext(AnalyticsContext)
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
        setEditPlayer(player); 
        setEditPlayerDrawerOpen(true)
    }

    return (
        <Card style={{
            height: '100%',
            overflow: 'auto',
            margin: '4px',
            border: '1px solid rgba(140, 140, 140, 0.35)',
          }}>
            
            <List itemLayout="horizontal"  
                  size="small" style={{ marginLeft: '-18px', height: '1px' /* Now sure why but this 1px causes the list height not be extended  */}}
                  dataSource={props.players} 
                  renderItem={(player, index) => (
                    <List.Item actions={[ <Button onClick={()=>{openEditPlayerViewHandler(player)}} icon={<EditOutlined /> } style={{marginRight: '-10px'}}/> , <div style={{marginRight: '-10px'}}><AppCheckBox  value={player.isArrived} onChanged={(e)=>{setPlayerArrivedHandler(player, e)}} /></div>, <Popconfirm icon={<QuestionCircleOutlined style={{ color: 'red' }} />} title='Remove Player' description='Are you sure you want to remove this player?' onConfirm={()=>{playerListItemClicked(player) }}>
                        <Button style={{marginTop: '15px', marginRight: '-30px'}} icon={<DeleteOutlined />} danger></Button></Popconfirm>]}>
                            
                        <List.Item.Meta avatar={ <Avatar style={{ marginTop: '20px' }}><UserOutlined  style={{ fontSize: '30px'}} /></Avatar>}
                                        title={<div style={{ fontSize: '12px', width: '100%', 'textOverflow': 'ellipsis', 'whiteSpace': 'nowrap', 'overflow': 'hidden', marginBottom: '-8px' }}>{player.Name.toUpperCase()}</div>}
                                        description={
                                            <label style={{fontSize: '12px'}}>{player.isArrived ? 'Joined' : 'Absent'}</label>
                                        }/>

                    </List.Item>
                )}>

            </List>


            {props.currentAlgo && <Drawer title="Edit Player" 
                        width='75%'
                        onClose={() => { setEditPlayerDrawerOpen(false) }}
                        open={editPlayerDrawerOpen}>
                    <AddPlayerForm onEditFinished={() => { setEditPlayerDrawerOpen(false)}} player={editPlayer} playersProperties={props.currentAlgo.playerProperties}/>
                </Drawer>}

        </Card>
    )
}

export default MobilePlayersList;