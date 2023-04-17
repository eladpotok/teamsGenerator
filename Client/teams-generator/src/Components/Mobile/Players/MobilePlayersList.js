import { DeleteOutlined, EditOutlined, QuestionCircleOutlined, UserOutlined } from "@ant-design/icons";
import { Avatar, Button, Card, Drawer, List, Popconfirm } from "antd";
import { useState } from "react";
import AddPlayerForm from "../../Common/AddPlayerForm";

function MobilePlayersList(props) {
    const [editPlayerDrawerOpen, setEditPlayerDrawerOpen] = useState(false)
    const [editPlayer, setEditPlayer] = useState(null)

    function playerListItemClicked(clickedPlayer) {
        props.onPlayerRemoved(clickedPlayer)
    }


    return (
        <Card style={{
            height: '100%',
            overflow: 'auto',
            
            border: '1px solid rgba(140, 140, 140, 0.35)',
          }}>
            
            <List itemLayout="horizontal"  
                  size="small" style={{ marginLeft: '-18px', marginTop: '-20px', height: '1px' /* Now sure why but this 1px causes the list height not be extended  */}}
                  dataSource={props.players} 
                  renderItem={(player, index) => (
                    <List.Item actions={[ <Button onClick={() => { setEditPlayer(player); setEditPlayerDrawerOpen(true) }} icon={<EditOutlined />}/> , <Popconfirm icon={<QuestionCircleOutlined style={{ color: 'red' }} />} title='Remove Player' description='Are you sure you want to remove this player?' onConfirm={()=>{playerListItemClicked(player) }}>
                        <Button style={{marginTop: '15px', marginRight: '-18px'}} icon={<DeleteOutlined />} danger></Button></Popconfirm>]}>
                        <List.Item.Meta avatar={<Avatar style={{ marginTop: '20px' }}><UserOutlined  style={{ fontSize: '30px'}} /></Avatar>}
                                        title={<div style={{width: '100%', 'textOverflow': 'ellipsis', 'whiteSpace': 'nowrap', 'overflow': 'hidden', marginBottom: '-8px' }}>{player.Name.toUpperCase()}</div>}
                                        description={
                                            <label style={{fontSize: '12px'}}>{`${getDescription(props.playerProperties, player)}`}</label>
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

function getDescription(playerProperties, player) {

    return "Scored: 4"

    var max = {
        value: 0,
        name: 'Attack'
    }

    var min = {
        value: 10,
        name: 'Attack'
    }

    playerProperties.forEach(prop => {
        const playerPropValue = player[prop.name]
        if (playerPropValue > max.value) {
            max.name = prop.name
            max.value = playerPropValue
        }
        if (playerPropValue < min.value) {
            min.name = prop.name
            min.value = playerPropValue
        }
    });

    return `${max.name}: ${max.value}  ${min.name}: ${min.value}`
}

export default MobilePlayersList;